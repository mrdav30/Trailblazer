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
            var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);
            var request = new TraversalRequest
            {
                Origin = scout.Position,
                Rotation = scout.Rotation,
                Direction = Vector3d.Zero,
                Rate = TrekRate.Stationary,
                IsRequestingJump = true
            };

            // Act
            scout.Motor.Traverse(scout, request);
            scout.CommitFrameMotion();

            // Assert
            scout.Motor.Locomotions.Move.FrameVelocity.y.Should().BeGreaterThan(Fixed64.Zero);
        }

        [Fact]
        public void Given_AirborneScout_When_JumpIsTriggered_Then_ShouldNotApplyJumpForce()
        {
            // Arrange
            var scout = MockMotorAgentTestFactory.CreateMockAgent(
                startPosition: Vector3d.Up,
                startVelocity: Vector3d.Down,
                startingMedium: TraversalMedium.Air);

            Vector3d expectedVelocity = Vector3d.Down;
            expectedVelocity.y += -scout.Motor.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;

            // Act
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            // Assert
            scout.Motor.Locomotions.Move.FrameVelocity.Should().BeApproximately(expectedVelocity, Fixed64.Epsilon);
        }

        [Fact]
        public void Given_ScoutThatJumped_When_JumpCooldownNotExpired_Then_ShouldNotJump()
        {
            // Arrange
            var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            Fixed64 expectedJumpFrame = scout.Motor.Locomotions.Jump.JumpStartTime;

            // Attempt to jump again immediately
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            // Assert
            scout.Motor.IsInAir.Should().BeTrue();
            scout.Motor.Locomotions.Jump.IsCoolingDown.Should().BeTrue();
            scout.Motor.Locomotions.Jump.JumpStartTime.Should().Be(expectedJumpFrame);
        }

        [Fact]
        public void Given_ScoutJumps_When_JumpIsReleasedMidAir_Then_GravityShouldResume()
        {
            // Arrange
            var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - First Jump
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            // Release jump after 2 frames
            for (int i = 0; i < 29; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            // Act - Simulate next frame
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            // Assert
            scout.Motor.Locomotions.Fall.IsFalling.Should().BeFalse();
            scout.Motor.Locomotions.Jump.IsJumping.Should().BeFalse();
            scout.Motor.Locomotions.Jump.IsCoolingDown.Should().BeFalse(); // default cool down is .2 seconds, which would take 7 frames, we simulate 31
            scout.Motor.Locomotions.Move.FrameVelocity.y.Should().Be(Fixed64.Zero); // Ground Force should have kicked in
        }

        [Fact]
        public void Given_ScoutCannotAffordJump_When_JumpRequested_Then_ShouldNotJump()
        {
            // Arrange
            var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);
            scout.Motor.Events.CanAffordJump = () => false;

            // Act
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            // Assert
            scout.Motor.Locomotions.Move.FrameVelocity.y.Should().Be(Fixed64.Zero); // Jump should not apply
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_Simulated_Then_GravityShouldBeTemporarilyReduced()
        {
            // Arrange
            var scout = MockMotorAgentTestFactory.CreateMockAgent(startingMedium: TraversalMedium.Ground);

            // Act - Initial Jump
            TrailblazerManager.Simulate();
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            Vector3d previousVelocity = scout.Motor.Locomotions.Move.FrameVelocity;

            // Continue holding jump for 3 frames
            for (int i = 0; i < 3; i++)
            {
                TrailblazerManager.Simulate();
                scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            // Assert
            var expected = previousVelocity.y - (scout.Motor.Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime * 3);
            scout.Motor.Locomotions.Move.FrameVelocity.y.Should().BeGreaterThan(expected);
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpHeld_Then_ShouldJumpHigher()
        {
            var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

            scout.ApplyInputTravelRequest(isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.ApplyInputTravelRequest(isRequestingJump: true);
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Position.y.Should().BeGreaterThan(scout.Motor.Locomotions.Jump.BaseJumpHeight); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutOnGround_When_JumpNotHeld_Then_ShouldNotJumpHigher()
        {
            var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

            scout.ApplyInputTravelRequest(isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            for (int i = 0; i < 13; i++)
            {
                TrailblazerManager.Simulate();
                scout.ApplyInputTravelRequest(isRequestingJump: false);
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Position.y.Should().BeGreaterThan(scout.Motor.Locomotions.Jump.BaseJumpHeight + Fixed64.Epsilon); // Higher than default jump height
        }

        [Fact]
        public void Given_ScoutWhen_JumpingAgainstCeiling_Then_ShouldStopRising()
        {
            var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent(startPosition: new Vector3d(0, 5, 0));

            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Slow, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            scout.Simulate();

            scout.SetTraversalCondition(TraversalMedium.Air, surfaceLevel: Fixed64.FromRaw(5 << 16), ceilingLevel: Fixed64.FromRaw(6 << 16)); // Simulate a ceiling

            scout.CommitFrameMotion();

            scout.Motor.Locomotions.Jump.IsJumping.Should().BeFalse(); // Jump should be canceled
            scout.Motor.Locomotions.Move.FrameVelocity.y.Should().BeLessThanOrEqualTo(Fixed64.Zero); // Should stop rising
        }

        [Fact]
        public void Given_ScoutHoldingJump_When_LandsOnGround_Then_ShouldResetJumpState()
        {
            var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

            bool jumpStarted = false;
            bool jumpStopped = false;
            bool fallStarted = false;
            bool fallStopped = false;

            scout.Motor.Events.OnStartJump += (avoidTimer) => jumpStarted = true;
            scout.Motor.Events.OnStopJump += () => jumpStopped = true;
            scout.Motor.Events.OnStartFall += () => fallStarted = true;
            scout.Motor.Events.OnLandedFall += () => fallStopped = true;

            // Start Jump
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Slow, isRequestingJump: true);
            scout.Simulate();
            scout.CommitFrameMotion();

            // Simulate entire jump arc until landing
            for (int i = 0; i < 30; i++)
            {
                TrailblazerManager.Simulate();
                scout.Simulate();
                scout.CommitFrameMotion();
                if (scout.Position.y <= Fixed64.Zero) // If we've landed
                    break;
            }

            scout.Simulate();

            scout.SetTraversalCondition(TraversalMedium.Ground, surfaceLevel: Fixed64.Zero);

            scout.CommitFrameMotion();

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
            var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

            var jumpLocomotion = scout.Motor.Locomotions.Jump;
            Fixed64 maxExpectedHeight = jumpLocomotion.BaseJumpHeight + jumpLocomotion.ExtraJumpHeight;

            Fixed64 maxY = scout.Position.y;

            // Start jump
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            // Continue holding jump and track peak height until we land
            while (!scout.Motor.IsGrounded)
            {
                TrailblazerManager.Simulate();
                scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
                scout.Simulate();
                scout.CommitFrameMotion();

                if (scout.Position.y > maxY)
                    maxY = scout.Position.y;
            }

            maxY.Should().BeLessThanOrEqualTo(maxExpectedHeight);
        }

        [Fact]
        public void Given_ScoutTapsJump_When_ReleasedImmediately_Then_JumpHeightShouldBeReduced()
        {
            var scout = MockMotorAgentTestFactory.CreateJumpReadyAgent();

            // Start jump
            scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: true);
            TrailblazerManager.Simulate();
            scout.Simulate();
            scout.CommitFrameMotion();

            // Tap release
            for (int i = 0; i < 5; i++)
            {
                TrailblazerManager.Simulate();
                scout.ApplyInputTravelRequest(Vector3d.Zero, rate: TrekRate.Stationary, isRequestingJump: false);
                scout.Simulate();
                scout.CommitFrameMotion();
            }

            scout.Position.y.Should().BeGreaterThan((Fixed64)0.5).And.BeLessThan((Fixed64)1.0);
        }
    }
}
